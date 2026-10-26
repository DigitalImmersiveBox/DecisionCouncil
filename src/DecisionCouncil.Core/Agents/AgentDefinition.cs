namespace DecisionCouncil.Core.Agents;

/// <summary>An agent as defined by one markdown file: front matter for settings, body for the system prompt.</summary>
public sealed class AgentDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Council category the agent is grouped under (see <see cref="AgentCategories"/>). Never empty after loading.</summary>
    public string Category { get; set; } = AgentCategories.Other;

    /// <summary>Place inside its category: the lead comes first.</summary>
    public int Order { get; set; } = 100;
    public bool Enabled { get; set; } = true;
    public int WordLimit { get; set; } = 250;
    public string Color { get; set; } = "#6B707B";
    public string Summary { get; set; } = "";
    public string CoreQuestion { get; set; } = "";

    /// <summary>Pixel avatar shape (crab, antenna, robot, cat, owl, ghost, dib). Empty picks one from the id.</summary>
    public string Avatar { get; set; } = "";

    /// <summary>The markdown body after the front matter: mandate, push-back, tone. Becomes the system prompt.</summary>
    public string Body { get; set; } = "";

    public string FilePath { get; set; } = "";

    public override string ToString() => Name;
}
