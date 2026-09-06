namespace Centra.Events;

public static class CloudEventConstants
{
    public const string SpecVersion10 = "1.0";
    public const string DefaultContentType = "application/json";

    // Standard CloudEvents Attributes
    public const string IdAttribute = "id";
    public const string SourceAttribute = "source";
    public const string SpecVersionAttribute = "specversion";
    public const string TypeAttribute = "type";
    public const string DataContentTypeAttribute = "datacontenttype";
    public const string DataSchemaAttribute = "dataschema";
    public const string SubjectAttribute = "subject";
    public const string TimeAttribute = "time";
    public const string DataAttribute = "data";

    // HTTP / Binary Mode Headers
    public const string IdHeader = "ce-id";
    public const string SourceHeader = "ce-source";
    public const string SpecVersionHeader = "ce-specversion";
    public const string TypeHeader = "ce-type";
    public const string DataContentTypeHeader = "ce-datacontenttype";
    public const string DataSchemaHeader = "ce-dataschema";
    public const string SubjectHeader = "ce-subject";
    public const string TimeHeader = "ce-time";

    // Enterprise Extensions
    public const string CorrelationIdAttribute = "correlationid";
    public const string CorrelationIdHeader = "ce-correlationid";

    public const string CausationIdAttribute = "causationid";
    public const string CausationIdHeader = "ce-causationid";

    public const string TenantIdAttribute = "tenantid";
    public const string TenantIdHeader = "ce-tenantid";

    public const string SchemaVersionAttribute = "schemaversion";
    public const string SchemaVersionHeader = "ce-schemaversion";

    // Distributed Tracing Headers (W3C)
    public const string TraceParentHeader = "traceparent";
    public const string TraceStateHeader = "tracestate";
}
