namespace OutfitFinder.Core.Entities;

public enum SearchStatus { Pending, Running, Completed, Failed, Rejected }

/// <summary>Live step shown on the loading screen.</summary>
public enum SearchStage { Queued, CheckingPhoto, FindingPieces, SearchingStores, CheckingLinks, Done }

public enum SearchSource { Web, ExtensionUpload, ExtensionUrl }

public enum MatchKind { Exact, Similar }

public enum MatchFeedback { Up, Down }

public enum PatternJobStatus { Pending, Completed, Failed }

public enum MaterialKind { Fabric, Notion, Thread, Interfacing }

public enum ReportKind { Problem, Takedown }

public enum AiProviderKind { AzureAiFoundry, OpenAI, Google, Demo }
