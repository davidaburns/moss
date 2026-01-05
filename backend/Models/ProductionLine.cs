namespace Moss.Models;

public record ProductionLineDto {
    public required string Id;
    public required string Name;
    public required ProductionAssetDto[] Assets;
    public required bool Active;

    public required DateTime Created;
    public required string CreatedBy;
    public DateTime? Updated;
    public string? UpdatedBy;
}
