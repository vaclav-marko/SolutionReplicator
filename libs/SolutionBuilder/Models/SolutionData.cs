namespace SolutionBuilder.Models;

/// <summary>
/// Represents the entire parsed solution metadata needed for the UI tree and processing.
/// </summary>
public class SolutionData
{
    public string SolutionId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string SourceCountry { get; set; } = string.Empty; // e.g. "DE"
    public string ProductAlias { get; set; } = string.Empty;
    public string ProductVersionAlias { get; set; } = string.Empty;
    public List<ScopeItem> ScopeItems { get; set; } = new();
    public List<BuildingBlock> BuildingBlocks { get; set; } = new();
}

/// <summary>
/// Represents a Scope Item (from SCENARIOS_SEQUENCE)
/// </summary>
public class ScopeItem
{
    public string ProjectId { get; set; } = string.Empty; // e.g. "DE_BD3_OP"
    public string SirSiCountry { get; set; } = string.Empty; // e.g. "DE"
    public string SirSiId { get; set; } = string.Empty; // e.g. "BD3"
    public string Description { get; set; } = string.Empty;
    public List<string> BuildingBlockIds { get; set; } = new(); // BB_SEQUENCE references
}

/// <summary>
/// Represents a Building Block (from BUILDING_BLOCKS)
/// </summary>
public class BuildingBlock
{
    public string BbId { get; set; } = string.Empty; // e.g. "1IM (XX)" or "BLV (DE)"
    public string SirBbId { get; set; } = string.Empty; // e.g. "1IM"
    public string SirBbCountry { get; set; } = string.Empty; // e.g. "XX" or "DE"
    public string Description { get; set; } = string.Empty;
    public bool IsGlobal => SirBbCountry == "XX";
    public List<TaskActivity> Tasks { get; set; } = new();
    public List<string> ImgSubObjectFiles { get; set; } = new(); // filenames from IMG_SUBOBJECTS
}

/// <summary>
/// Represents a Task/Activity within a Building Block
/// </summary>
public class TaskActivity
{
    public string ObjectId { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
}