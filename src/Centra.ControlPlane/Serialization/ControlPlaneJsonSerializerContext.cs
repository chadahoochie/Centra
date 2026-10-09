using System.Text.Json;
using System.Text.Json.Serialization;
using Centra.Components;
using Centra.ControlPlane.Actors;
using Centra.ControlPlane.Catalog;
using Centra.ControlPlane.Diagnostics;
using Centra.ControlPlane.Security;
using Centra.ControlPlane.Topology;
using Centra.ControlPlane.Workflows;
using Centra.Core.Workflows;
using Centra.Sync;

namespace Centra.ControlPlane.Serialization;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ClusterAdmissionErrorResponse))]
[JsonSerializable(typeof(ComponentDefinition))]
[JsonSerializable(typeof(List<ComponentDefinition>))]
[JsonSerializable(typeof(IReadOnlyCollection<ComponentDefinition>))]
[JsonSerializable(typeof(ComponentCatalogEntry))]
[JsonSerializable(typeof(List<ComponentCatalogEntry>))]
[JsonSerializable(typeof(IReadOnlyCollection<ComponentCatalogEntry>))]
[JsonSerializable(typeof(ResiliencePolicyDto))]
[JsonSerializable(typeof(List<ResiliencePolicyDto>))]
[JsonSerializable(typeof(IReadOnlyCollection<ResiliencePolicyDto>))]
[JsonSerializable(typeof(ResiliencePolicyCatalogEntry))]
[JsonSerializable(typeof(List<ResiliencePolicyCatalogEntry>))]
[JsonSerializable(typeof(IReadOnlyCollection<ResiliencePolicyCatalogEntry>))]
[JsonSerializable(typeof(ResilienceSyncEventDto))]
[JsonSerializable(typeof(ComponentSyncEventDto))]
[JsonSerializable(typeof(HeartbeatRequest))]
[JsonSerializable(typeof(HeartbeatResponse))]
[JsonSerializable(typeof(ClientNodeInfo))]
[JsonSerializable(typeof(List<ClientNodeInfo>))]
[JsonSerializable(typeof(IReadOnlyCollection<ClientNodeInfo>))]
[JsonSerializable(typeof(ControlPlaneHealthResponse))]
[JsonSerializable(typeof(ActorActivationCountResponse))]
[JsonSerializable(typeof(ActorPassivateResponse))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(WorkflowDefinitionDto))]
[JsonSerializable(typeof(List<WorkflowDefinitionDto>))]
[JsonSerializable(typeof(WorkflowActivityDto))]
[JsonSerializable(typeof(List<WorkflowActivityDto>))]
[JsonSerializable(typeof(WorkflowInstanceStateDto))]
[JsonSerializable(typeof(WorkflowHistoryEventRecord))]
[JsonSerializable(typeof(List<WorkflowHistoryEventRecord>))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
internal sealed partial class ControlPlaneJsonSerializerContext : JsonSerializerContext
{
}
