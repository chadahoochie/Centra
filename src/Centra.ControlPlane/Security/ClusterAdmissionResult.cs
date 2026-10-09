namespace Centra.ControlPlane.Security;

public readonly record struct ClusterAdmissionResult(bool IsSuccess, int StatusCode, string? ErrorMessage)
{
    public static ClusterAdmissionResult Success() => new(true, 200, null);
    public static ClusterAdmissionResult Unauthorized(string message) => new(false, 401, message);
    public static ClusterAdmissionResult Forbidden(string message) => new(false, 403, message);
    public static ClusterAdmissionResult BadRequest(string message) => new(false, 400, message);
}
