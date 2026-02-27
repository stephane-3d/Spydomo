namespace Spydomo.DTO
{
    public sealed record TextSample(
        int CompanyId,
        string SourceType,      // "Reddit","G2",...
        int? RawContentId,
        int? SummarizedInfoId,
        DateTime SeenAt,
        string Text
    );
}
