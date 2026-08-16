using CloudOrders.Application.Abstractions;

namespace CloudOrders.Infrastructure.Identity;

public sealed class LocalDevelopmentSubjectIdProvider : ISubjectIdProvider
{
    public const string DevelopmentSubjectId = "local-development-subject";

    public string SubjectId => DevelopmentSubjectId;
}
