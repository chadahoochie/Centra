using System.Collections.Concurrent;
using Centra.Actors;
using Centra.State;
using Microsoft.Extensions.DependencyInjection;

namespace Centra.Core.Actors;

/// <summary>
/// Thread-safe registry and lifecycle manager for virtual actor activations.
/// </summary>
public sealed class ActorManager : IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IStateStore _stateStore;
    private readonly ActorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<ActorIdentity, Task<ActorActivation>> _activations = new();
    private readonly ConcurrentDictionary<ActorType, Type> _registeredActorTypes = new();
    private int _isDisposed;

    public int ActiveCount => _activations.Count;

    public ActorManager(
        IServiceProvider serviceProvider,
        IStateStore stateStore,
        ActorOptions options,
        TimeProvider? timeProvider = null,
        IEnumerable<ActorRegistration>? registrations = null)
    {
        _serviceProvider = serviceProvider;
        _stateStore = stateStore;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (registrations != null)
        {
            foreach (var reg in registrations)
            {
                RegisterActorType(reg.ActorType);
            }
        }
    }

    public void RegisterActorType(Type actorType)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        _registeredActorTypes[ActorType.FromType(actorType)] = actorType;
    }

    public void RegisterActorType<TActor>() where TActor : Actor
    {
        RegisterActorType(typeof(TActor));
    }

    public async ValueTask<TResult> DispatchAsync<TResult>(
        ActorIdentity identity,
        Func<Actor, ValueTask<TResult>> invoker,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);

        var activation = await GetOrCreateActivationAsync(identity, cancellationToken).ConfigureAwait(false);
        activation.LastAccessedUtc = _timeProvider.GetUtcNow();

        return await activation.Mailbox.EnqueueTurnAsync(async () =>
        {
            var result = await invoker(activation.Instance).ConfigureAwait(false);
            await activation.StateManager.SaveStateAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DispatchAsync(
        ActorIdentity identity,
        Func<Actor, ValueTask> invoker,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);

        var activation = await GetOrCreateActivationAsync(identity, cancellationToken).ConfigureAwait(false);
        activation.LastAccessedUtc = _timeProvider.GetUtcNow();

        await activation.Mailbox.EnqueueTurnAsync(async () =>
        {
            await invoker(activation.Instance).ConfigureAwait(false);
            await activation.StateManager.SaveStateAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> PassivateActorAsync(ActorIdentity identity, CancellationToken cancellationToken = default)
    {
        if (_activations.TryRemove(identity, out var activationTask))
        {
            try
            {
                var activation = await activationTask.ConfigureAwait(false);
                await activation.StateManager.SaveStateAsync(cancellationToken).ConfigureAwait(false);
                await activation.Instance.OnDeactivateAsync(cancellationToken).ConfigureAwait(false);
                await activation.Timers.DisposeAsync().ConfigureAwait(false);
                await activation.Mailbox.DisposeAsync().ConfigureAwait(false);
                return true;
            }
            catch (Exception)
            {
                // Ensure passivation completes even if deactivation throws
                return true;
            }
        }

        return false;
    }

    public async ValueTask<int> PassivateIdleActorsAsync(TimeSpan? idleTimeout = null, CancellationToken cancellationToken = default)
    {
        var timeout = idleTimeout ?? _options.ActorIdleTimeout;
        var now = _timeProvider.GetUtcNow();
        var passivatedCount = 0;

        foreach (var (identity, task) in _activations)
        {
            if (!task.IsCompletedSuccessfully)
            {
                continue;
            }

            var activation = task.Result;
            if (now - activation.LastAccessedUtc < timeout)
            {
                continue;
            }

            if (await PassivateActorAsync(identity, cancellationToken).ConfigureAwait(false))
            {
                passivatedCount++;
            }
        }

        return passivatedCount;
    }

    internal async ValueTask<ActorActivation> GetOrCreateActivationAsync(
        ActorIdentity identity,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_activations.TryGetValue(identity, out var existingTask))
            {
                try
                {
                    return await existingTask.ConfigureAwait(false);
                }
                catch
                {
                    _activations.TryRemove(identity, out _);
                }
            }

            var tcs = new TaskCompletionSource<ActorActivation>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_activations.TryAdd(identity, tcs.Task))
            {
                try
                {
                    var activation = await CreateActivationCoreAsync(identity, cancellationToken).ConfigureAwait(false);
                    tcs.SetResult(activation);
                    return activation;
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                    _activations.TryRemove(identity, out _);
                    throw;
                }
            }
        }
    }

    private async Task<ActorActivation> CreateActivationCoreAsync(
        ActorIdentity identity,
        CancellationToken cancellationToken)
    {
        var actorType = ResolveActorType(identity.Type);
        var instance = (Actor)ActivatorUtilities.GetServiceOrCreateInstance(_serviceProvider, actorType);

        var mailbox = new ActorMailbox();
        var stateManager = new ActorStateManager(identity, _stateStore, _options.DefaultStateStore);
        var timers = new ActorTimerManager(identity, _timeProvider);
        var coordinator = _serviceProvider.GetService<ActorReminderCoordinator>();
        var reminders = new ActorReminderManager(identity, _stateStore, _options.DefaultStateStore, coordinator);

        instance.Identity = identity;
        instance.StateManager = stateManager;
        instance.Timers = timers;
        instance.Reminders = reminders;

        var activation = new ActorActivation(
            identity,
            instance,
            mailbox,
            stateManager,
            timers,
            reminders,
            _timeProvider.GetUtcNow());

        await instance.OnActivateAsync(cancellationToken).ConfigureAwait(false);

        return activation;
    }

    private Type ResolveActorType(ActorType type)
    {
        if (_registeredActorTypes.TryGetValue(type, out var registeredType))
        {
            return registeredType;
        }

        // Scan assemblies for matching type name
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;
            var match = assembly.GetTypes().FirstOrDefault(t =>
                !t.IsAbstract && typeof(Actor).IsAssignableFrom(t) && t.Name == type.Value);

            if (match != null)
            {
                _registeredActorTypes[type] = match;
                return match;
            }
        }

        throw new InvalidOperationException($"No actor implementation type found matching '{type.Value}'.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            foreach (var identity in _activations.Keys)
            {
                await PassivateActorAsync(identity).ConfigureAwait(false);
            }
        }
    }
}
