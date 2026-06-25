using StateManagementPoc.Models;

namespace StateManagementPoc.Services;

public class CaseRepository
{
    private static readonly List<CaseDto> _cases = new()
    {
        new CaseDto(1, "Breach of Contract"),
        new CaseDto(2, "Employment Dispute"),
        new CaseDto(3, "Personal Injury"),
        new CaseDto(4, "Intellectual Property"),
        new CaseDto(5, "Regulatory Compliance"),
    };

    public IReadOnlyList<CaseDto> GetAll() => _cases;

    public CaseDto? GetById(int id) => _cases.FirstOrDefault(c => c.Id == id);
}
