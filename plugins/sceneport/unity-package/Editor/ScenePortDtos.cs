using System.Collections.Generic;
using Newtonsoft.Json;

namespace ScenePort.McpBridge.Editor
{
    // Response DTOs. Every field is explicitly named with [JsonProperty] so the wire
    // contract consumed by the MCP server (plugins/sceneport/server/src/unityClient.ts)
    // can never drift with C# naming conventions. Field names and shapes mirror the
    // original hand-rolled JSON exactly.

    internal sealed class ErrorResponse
    {
        [JsonProperty("status")] public string Status = "error";
        [JsonProperty("error")] public string Error;
        [JsonProperty("message")] public string Message;
        [JsonProperty("code")] public string Code;
        [JsonProperty("category")] public string Category;
        [JsonProperty("retryable")] public bool Retryable;
        [JsonProperty("retryAfterMs", NullValueHandling = NullValueHandling.Ignore)] public int? RetryAfterMs;
        [JsonProperty("remediation", NullValueHandling = NullValueHandling.Ignore)] public string Remediation;
        [JsonProperty("details", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, object> Details;

        internal ErrorResponse() { }
        internal ErrorResponse(string error)
        {
            Error = error ?? string.Empty;
            Message = Error;
            Code = "operation.failed";
            Category = "bridge";
            Retryable = false;
        }

        internal ErrorResponse(
            string code,
            string message,
            string category = "bridge",
            bool retryable = false,
            int? retryAfterMs = null,
            string remediation = null,
            Dictionary<string, object> details = null)
        {
            Code = string.IsNullOrEmpty(code) ? "operation.failed" : code;
            Error = message ?? string.Empty;
            Message = Error;
            Category = string.IsNullOrEmpty(category) ? "bridge" : category;
            Retryable = retryable;
            RetryAfterMs = retryAfterMs;
            Remediation = remediation;
            Details = details;
        }
    }

    internal sealed class HealthResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("bridge")] public string Bridge = "sceneport";
        [JsonProperty("version")] public string Version;
        [JsonProperty("unityVersion")] public string UnityVersion;
        [JsonProperty("projectPath")] public string ProjectPath;
        [JsonProperty("activeScene")] public string ActiveScene;
        [JsonProperty("isPlaying")] public bool IsPlaying;
        [JsonProperty("isCompiling")] public bool IsCompiling;
        [JsonProperty("isUpdating")] public bool IsUpdating;
        [JsonProperty("port")] public int Port;

        // Added in v0.3.0 (project identity + auth capability advertisement).
        [JsonProperty("projectId")] public string ProjectId;
        [JsonProperty("projectName")] public string ProjectName;
        [JsonProperty("tokenRequired")] public bool TokenRequired;
        [JsonProperty("protocolVersion")] public int ProtocolVersion;
        [JsonProperty("capabilitiesHash")] public string CapabilitiesHash;
        [JsonProperty("ownerLeaseId")] public string OwnerLeaseId;
        [JsonProperty("heartbeatUtc")] public string HeartbeatUtc;
        [JsonProperty("startedUtc")] public string StartedUtc;
        [JsonProperty("editorRole")] public string EditorRole;
        [JsonProperty("processId")] public int ProcessId;
        [JsonProperty("processName")] public string ProcessName;
    }

    internal sealed class CapabilitiesResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("bridge")] public string Bridge = "sceneport";
        [JsonProperty("protocolVersion")] public int ProtocolVersion;
        [JsonProperty("bridgeVersion")] public string BridgeVersion;
        [JsonProperty("capabilitiesHash")] public string CapabilitiesHash;
        [JsonProperty("endpointGroups")] public string[] EndpointGroups;
        [JsonProperty("supportsAuditLog")] public bool SupportsAuditLog = true;
        [JsonProperty("supportsSafeWrites")] public bool SupportsSafeWrites = true;
        [JsonProperty("supportsPlaytests")] public bool SupportsPlaytests = true;
        [JsonProperty("supportsPerception")] public bool SupportsPerception = true;
        [JsonProperty("supportsProofLoop")] public bool SupportsProofLoop = true;
        [JsonProperty("supportsDiagnostics")] public bool SupportsDiagnostics = true;
        [JsonProperty("supportsAuthoring")] public bool SupportsAuthoring = true;
        [JsonProperty("supportsDryRun")] public bool SupportsDryRun = true;
        [JsonProperty("supportsTransactionalBatch")] public bool SupportsTransactionalBatch = true;
        [JsonProperty("supportsMenuItemAllowlist")] public bool SupportsMenuItemAllowlist = true;
        [JsonProperty("authoringSchemaVersion")] public int AuthoringSchemaVersion = 1;
        [JsonProperty("policy")] public PolicyDto Policy;
    }

    internal sealed class PolicyDto
    {
        [JsonProperty("profile")] public string Profile;
        [JsonProperty("allowedEndpointGroups")] public string[] AllowedEndpointGroups;
        [JsonProperty("deniedEndpointGroups")] public string[] DeniedEndpointGroups;
    }

    internal sealed class SceneResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("name")] public string Name;
        [JsonProperty("path")] public string Path;
        [JsonProperty("buildIndex")] public int BuildIndex;
        [JsonProperty("rootCount")] public int RootCount;
        [JsonProperty("isDirty")] public bool IsDirty;
        [JsonProperty("isLoaded")] public bool IsLoaded;
        [JsonProperty("isValid")] public bool IsValid;
    }

    internal sealed class HierarchyResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("scene")] public string Scene;
        [JsonProperty("objects")] public List<HierarchyNode> Objects = new List<HierarchyNode>();
        [JsonProperty("truncated")] public bool Truncated;
    }

    internal sealed class HierarchyNode
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("path")] public string Path;
        [JsonProperty("instanceId")] public int InstanceId;
        [JsonProperty("active")] public bool Active;
        [JsonProperty("activeInHierarchy")] public bool ActiveInHierarchy;
        [JsonProperty("depth")] public int Depth;
        [JsonProperty("childCount")] public int ChildCount;
        [JsonProperty("components")] public List<string> Components = new List<string>();
    }

    internal sealed class GameObjectRef
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("path")] public string Path;
        [JsonProperty("instanceId")] public int InstanceId;
        [JsonProperty("active")] public bool Active;
        [JsonProperty("activeInHierarchy")] public bool ActiveInHierarchy;
        [JsonProperty("childCount")] public int ChildCount;
    }

    internal sealed class SelectionResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("objects")] public List<GameObjectRef> Objects = new List<GameObjectRef>();
    }

    internal sealed class LogEntryDto
    {
        [JsonProperty("sequence")] public long Sequence;
        [JsonProperty("type")] public string Type;
        [JsonProperty("utc")] public string Utc;
        [JsonProperty("message")] public string Message;
        [JsonProperty("stackTrace")] public string StackTrace;
    }

    internal sealed class ConsoleResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("logs")] public List<LogEntryDto> Logs = new List<LogEntryDto>();
    }

    internal sealed class PageDto
    {
        [JsonProperty("limit")] public int Limit;
        [JsonProperty("nextCursor", NullValueHandling = NullValueHandling.Ignore)] public string NextCursor;
        [JsonProperty("truncated")] public bool Truncated;
    }

    internal sealed class SceneQueryItemDto
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("path")] public string Path;
        [JsonProperty("instanceId")] public int InstanceId;
        [JsonProperty("active")] public bool Active;
        [JsonProperty("activeInHierarchy")] public bool ActiveInHierarchy;
        [JsonProperty("tag")] public string Tag;
        [JsonProperty("layer")] public int Layer;
        [JsonProperty("scene")] public string Scene;
        [JsonProperty("depth")] public int Depth;
        [JsonProperty("childCount")] public int ChildCount;
        [JsonProperty("components", NullValueHandling = NullValueHandling.Ignore)] public List<ComponentDto> Components;
        [JsonProperty("transform", NullValueHandling = NullValueHandling.Ignore)] public TransformDto Transform;
    }

    internal sealed class SceneQueryResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("items")] public List<SceneQueryItemDto> Items = new List<SceneQueryItemDto>();
        [JsonProperty("page")] public PageDto Page;
    }

    internal sealed class ComponentQueryItemDto
    {
        [JsonProperty("object")] public GameObjectRef Object;
        [JsonProperty("component")] public ComponentDto Component;
    }

    internal sealed class ComponentQueryResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("items")] public List<ComponentQueryItemDto> Items = new List<ComponentQueryItemDto>();
        [JsonProperty("page")] public PageDto Page;
    }

    internal sealed class Vector3Dto
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
        [JsonProperty("z")] public float Z;

        internal Vector3Dto() { }

        internal Vector3Dto(UnityEngine.Vector3 v)
        {
            X = v.x;
            Y = v.y;
            Z = v.z;
        }
    }

    internal sealed class TransformDto
    {
        [JsonProperty("localPosition")] public Vector3Dto LocalPosition;
        [JsonProperty("localEulerAngles")] public Vector3Dto LocalEulerAngles;
        [JsonProperty("localScale")] public Vector3Dto LocalScale;
        [JsonProperty("worldPosition")] public Vector3Dto WorldPosition;
    }

    internal sealed class PropertyDto
    {
        [JsonProperty("path")] public string Path;
        [JsonProperty("displayName")] public string DisplayName;
        [JsonProperty("type")] public string Type;
        [JsonProperty("editable")] public bool Editable;
        [JsonProperty("value")] public string Value;
    }

    internal sealed class TypedPropertyDto
    {
        [JsonProperty("path")] public string Path;
        [JsonProperty("displayName")] public string DisplayName;
        [JsonProperty("propertyType")] public string PropertyType;
        [JsonProperty("editable")] public bool Editable;
        [JsonProperty("depth")] public int Depth;
        [JsonProperty("hasChildren")] public bool HasChildren;
        [JsonProperty("valueKind")] public string ValueKind;
        [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)] public object Value;
        [JsonProperty("displayValue")] public string DisplayValue;
    }

    internal sealed class SerializedReadResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("target")] public ObjectRef Target;
        [JsonProperty("properties")] public List<TypedPropertyDto> Properties = new List<TypedPropertyDto>();
        [JsonProperty("page")] public PageDto Page;
    }

    internal sealed class ComponentDto
    {
        [JsonProperty("index")] public int Index;
        [JsonProperty("type")] public string Type;
        // Absent for missing scripts, mirroring the original output.
        [JsonProperty("fullType", NullValueHandling = NullValueHandling.Ignore)] public string FullType;
        [JsonProperty("assemblyQualifiedName", NullValueHandling = NullValueHandling.Ignore)] public string AssemblyQualifiedName;
        [JsonProperty("instanceId")] public int InstanceId;
        [JsonProperty("enabled")] public bool? Enabled;
        [JsonProperty("properties")] public List<PropertyDto> Properties = new List<PropertyDto>();
    }

    internal sealed class GameObjectDetail
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("path")] public string Path;
        [JsonProperty("instanceId")] public int InstanceId;
        [JsonProperty("active")] public bool Active;
        [JsonProperty("activeInHierarchy")] public bool ActiveInHierarchy;
        [JsonProperty("childCount")] public int ChildCount;
        [JsonProperty("tag")] public string Tag;
        [JsonProperty("layer")] public int Layer;
        [JsonProperty("scene")] public string Scene;
        [JsonProperty("transform")] public TransformDto Transform;
        // Omitted entirely when includeComponents is false, mirroring the original output.
        [JsonProperty("components", NullValueHandling = NullValueHandling.Ignore)] public List<ComponentDto> Components;
    }

    internal sealed class GameObjectDetailResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("object")] public GameObjectDetail Object;
    }

    internal sealed class GameObjectRefResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("object")] public GameObjectRef Object;
    }

    internal sealed class ComponentsResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("object")] public GameObjectRef Object;
        [JsonProperty("components")] public List<ComponentDto> Components = new List<ComponentDto>();
    }

    internal sealed class AddComponentResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("object")] public GameObjectRef Object;
        [JsonProperty("component")] public ComponentDto Component;
    }

    internal sealed class ObjectRef
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("instanceId")] public int InstanceId;
        [JsonProperty("type")] public string Type;
    }

    internal sealed class SetSerializedPropertyResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("target")] public ObjectRef Target;
        [JsonProperty("propertyPath")] public string PropertyPath;
        [JsonProperty("propertyType")] public string PropertyType;
    }

    internal sealed class AssetDto
    {
        [JsonProperty("guid")] public string Guid;
        [JsonProperty("path")] public string Path;
        [JsonProperty("name")] public string Name;
        [JsonProperty("type")] public string Type;
        [JsonProperty("labels")] public List<string> Labels = new List<string>();
    }

    internal sealed class AssetSearchResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("query")] public string Query;
        [JsonProperty("count")] public int Count;
        [JsonProperty("assets")] public List<AssetDto> Assets = new List<AssetDto>();
        [JsonProperty("truncated")] public bool Truncated;
    }

    internal sealed class CompilationStatusResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("isCompiling")] public bool IsCompiling;
        [JsonProperty("isUpdating")] public bool IsUpdating;
        [JsonProperty("isPlaying")] public bool IsPlaying;
        [JsonProperty("isPlayingOrWillChangePlaymode")] public bool IsPlayingOrWillChangePlaymode;
        [JsonProperty("timeSinceStartup")] public double TimeSinceStartup;
        [JsonProperty("recentErrors")] public List<LogEntryDto> RecentErrors = new List<LogEntryDto>();
    }

    internal sealed class FailedTestDto
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("message")] public string Message;
        [JsonProperty("stackTrace")] public string StackTrace;
    }

    internal sealed class CaptureGameViewResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("path")] public string Path;
        [JsonProperty("superSize")] public int SuperSize;
        [JsonProperty("note")] public string Note;

        // Inline image content (v0.9). Null unless the request asked for an inline capture.
        [JsonProperty("imageBase64", NullValueHandling = NullValueHandling.Ignore)] public string ImageBase64;
        [JsonProperty("width", NullValueHandling = NullValueHandling.Ignore)] public int? Width;
        [JsonProperty("height", NullValueHandling = NullValueHandling.Ignore)] public int? Height;
    }

    internal sealed class SceneViewResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("available")] public bool Available;
        [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)] public string Reason;
        [JsonProperty("cameraPosition", NullValueHandling = NullValueHandling.Ignore)] public Vector3Dto CameraPosition;
        [JsonProperty("cameraRotation", NullValueHandling = NullValueHandling.Ignore)] public Vector3Dto CameraRotation;
        [JsonProperty("orthographic")] public bool Orthographic;
        [JsonProperty("size")] public float Size;
    }

    internal sealed class RuntimeStatusResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("isPlaying")] public bool IsPlaying;
        [JsonProperty("isPaused")] public bool IsPaused;
        [JsonProperty("timeScale")] public float TimeScale;
        [JsonProperty("frameCount")] public int FrameCount;
        [JsonProperty("timeSinceStartup")] public double TimeSinceStartup;
    }

    internal sealed class ConsoleEventsResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("entries")] public List<LogEntryDto> Entries = new List<LogEntryDto>();
        [JsonProperty("oldestAvailableCursor")] public long OldestAvailableCursor;
        [JsonProperty("nextCursor")] public long NextCursor;
        [JsonProperty("cursorExpired")] public bool CursorExpired;
    }

    internal sealed class ProfilerSnapshotResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("totalAllocatedMemory")] public long TotalAllocatedMemory;
        [JsonProperty("totalReservedMemory")] public long TotalReservedMemory;
        [JsonProperty("monoUsedSize")] public long MonoUsedSize;
        [JsonProperty("monoHeapSize")] public long MonoHeapSize;
        [JsonProperty("frameCount")] public int FrameCount;
    }

    internal sealed class PlayModeResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("action")] public string Action;
        [JsonProperty("isPlaying")] public bool IsPlaying;
        [JsonProperty("isPlayingOrWillChangePlaymode")] public bool IsPlayingOrWillChangePlaymode;
    }

    internal sealed class DependencyDto
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("version")] public string Version;

        internal DependencyDto() { }
        internal DependencyDto(string name, string version) { Name = name; Version = version; }
    }

    internal sealed class PackagesResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("manifestPath")] public string ManifestPath;
        [JsonProperty("packagesLockPath")] public string PackagesLockPath;
        [JsonProperty("packagesLockExists")] public bool PackagesLockExists;
        [JsonProperty("dependencies")] public List<DependencyDto> Dependencies = new List<DependencyDto>();
    }

    internal sealed class AssetGraphResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("asset")] public AssetDto Asset;
        [JsonProperty("dependencies")] public List<AssetDto> Dependencies = new List<AssetDto>();
        [JsonProperty("referencers")] public List<AssetDto> Referencers = new List<AssetDto>();
        [JsonProperty("truncated")] public bool Truncated;
    }

    internal sealed class TestRunResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("run")] public TestRunSummaryDto Run;
    }

    internal sealed class TestArtifactsResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("runId")] public string RunId;
        [JsonProperty("directory")] public string Directory;
        [JsonProperty("summaryPath")] public string SummaryPath;
        [JsonProperty("consolePath")] public string ConsolePath;
    }

    internal sealed class AssertionResultDto
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("passed")] public bool Passed;
        [JsonProperty("message")] public string Message;
        [JsonProperty("actual", NullValueHandling = NullValueHandling.Ignore)] public object Actual;
    }

    internal sealed class AssertionEvaluateResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("passed")] public bool Passed;
        [JsonProperty("results")] public List<AssertionResultDto> Results = new List<AssertionResultDto>();
        [JsonProperty("artifactPath")] public string ArtifactPath;
    }

    internal sealed class MetricsResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("metrics")] public Dictionary<string, object> Metrics = new Dictionary<string, object>();
        [JsonProperty("artifactPath", NullValueHandling = NullValueHandling.Ignore)] public string ArtifactPath;
    }

    internal sealed class DiagnosticsResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("health")] public HealthResponse Health;
        [JsonProperty("capabilities")] public CapabilitiesResponse Capabilities;
        [JsonProperty("policy")] public PolicyDto Policy;
        [JsonProperty("auditPath")] public string AuditPath;
        [JsonProperty("recentAudit")] public List<AuditLogEntryDto> RecentAudit = new List<AuditLogEntryDto>();
        [JsonProperty("warnings")] public List<string> Warnings = new List<string>();
    }

    internal sealed class AuthoringChangeDto
    {
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("action")] public string Action;
        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)] public string Path;
        [JsonProperty("target", NullValueHandling = NullValueHandling.Ignore)] public string Target;
        [JsonProperty("undoSupported")] public bool UndoSupported;
        [JsonProperty("rollbackSupported")] public bool RollbackSupported;
    }

    internal sealed class AuthoringResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("dryRun")] public bool DryRun;
        [JsonProperty("operation")] public string Operation;
        [JsonProperty("changes")] public List<AuthoringChangeDto> Changes = new List<AuthoringChangeDto>();
        [JsonProperty("warnings")] public List<string> Warnings = new List<string>();
        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)] public object Result;
    }

    internal sealed class MenuItemAllowlistResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("items")] public string[] Items;
    }

    internal sealed class AuditLogEntryDto
    {
        [JsonProperty("utc")] public string Utc;
        [JsonProperty("method")] public string Method;
        [JsonProperty("endpoint")] public string Endpoint;
        [JsonProperty("status")] public string Status;
        [JsonProperty("summary")] public string Summary;
        [JsonProperty("target")] public string Target;
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)] public string Error;
        [JsonProperty("requestId", NullValueHandling = NullValueHandling.Ignore)] public string RequestId;
        [JsonProperty("dryRun")] public bool DryRun;
        [JsonProperty("transactional")] public bool Transactional;
        [JsonProperty("operation", NullValueHandling = NullValueHandling.Ignore)] public string Operation;
        [JsonProperty("operationCount")] public int OperationCount;
        [JsonProperty("undoGroup")] public int UndoGroup;
        [JsonProperty("paths")] public List<string> Paths = new List<string>();
        [JsonProperty("rolledBack")] public bool RolledBack;
    }

    internal sealed class AuditLogResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("path")] public string Path;
        [JsonProperty("entries")] public List<AuditLogEntryDto> Entries = new List<AuditLogEntryDto>();
    }
}
