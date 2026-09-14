namespace Centra.Hosting.Routing;

public sealed class CentraTopicRouterRegistry
{
    private readonly Dictionary<(string PubSubName, string Topic), CentraTopicRouter> _routers;
    private readonly IReadOnlyList<CentraTopicRouter> _routerList;

    public CentraTopicRouterRegistry(IEnumerable<CentraTopicRegistration> registrations)
    {
        var groups = registrations.GroupBy(r => (r.PubSubName, r.Topic));
        _routers = new Dictionary<(string, string), CentraTopicRouter>();
        var list = new List<CentraTopicRouter>();

        foreach (var group in groups)
        {
            var router = new CentraTopicRouter(group.Key.PubSubName, group.Key.Topic, group);
            _routers[group.Key] = router;
            list.Add(router);
        }

        _routerList = list;
    }

    public IReadOnlyList<CentraTopicRouter> GetRouters() => _routerList;

    public CentraTopicRouter? GetRouter(string pubSubName, string topic)
    {
        return _routers.TryGetValue((pubSubName, topic), out var router) ? router : null;
    }
}
