namespace Moss.Models {
    public record ProductionAssetDto {
        public required string Id;
        public required string Name;
        public string? OpcNodePath;
    }
}
