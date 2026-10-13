using System.ComponentModel.DataAnnotations;

namespace OrderPaymentMicroservice.Security;

public sealed class ApiAdmissionOptions
{
    [Range(1024, 1048576)] public int MaxBodyBytes { get; set; } = 65536;
    [Range(1, 1000)] public int MaxOrderItems { get; set; } = 100;
    [Range(1, 10000)] public int AuthenticationPermitLimit { get; set; } = 10;
    [Range(1, 100000)] public int ApiPermitLimit { get; set; } = 120;
    [Range(1, 3600)] public int WindowSeconds { get; set; } = 60;
}
