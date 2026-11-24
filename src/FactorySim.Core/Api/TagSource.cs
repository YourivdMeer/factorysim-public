using FactorySim.Core.Tags;

namespace FactorySim.Core.Api;

/// <summary>A tag store with the map it was built from; a new scene replaces both at once.</summary>
public sealed record TagSource(TagStore Store, TagMap Map);
