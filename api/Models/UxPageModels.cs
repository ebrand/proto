namespace Proto.Api.Models;

// A UX page belongs to a prototype (which belongs to a tenant). "illustrative"
// pages carry a mockup image (upload is a later feature — pages can be created
// without one); "functional" pages map to a route in the running app.

public sealed record CreateUxPageRequest(
    string Name,
    string Kind,           // "illustrative" | "functional"
    int? OrderIndex,
    bool? IsEntryPage,
    string? Route);

public sealed record UxPageSummary(
    string Id,
    string Name,
    string Kind,
    int OrderIndex,
    bool IsEntryPage,
    string? Route,
    string? ImageUrl,
    double? CanvasX,
    double? CanvasY,
    string CreatedAt);

// The (x, y) a page occupies on the prototype's flow-map canvas. Persisted when
// the user drags a frame; null until then (client auto-lays-out).
public sealed record UpdatePagePositionRequest(double X, double Y);
