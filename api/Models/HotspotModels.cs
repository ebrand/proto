namespace Proto.Api.Models;

// A hotspot is a clickable region on a UX page that links to another page (the
// flow-map arrows) or an external URL. Geometry is stored in normalized 0..1
// coordinates relative to the page, so a region survives any image size.
//
// v1 create supports rect only; polygon hotspots can be read but not yet
// created through the API. A link with no explicit rect defaults to the whole
// page (0,0,1,1) — enough to render a page->page arrow before image editing
// exists.

public sealed record CreateHotspotRequest(
    string? Label,
    double? RectX,
    double? RectY,
    double? RectWidth,
    double? RectHeight,
    string? TargetPageId,
    string? TargetExternalUrl);

public sealed record HotspotSummary(
    string Id,
    string UxPageId,
    string Shape,
    string? Label,
    double? RectX,
    double? RectY,
    double? RectWidth,
    double? RectHeight,
    string? TargetPageId,
    string? TargetExternalUrl,
    string CreatedAt);
