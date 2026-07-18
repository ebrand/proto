-- Spatial-canvas UX: each UX page has an (x, y) position on the prototype's
-- flow-map canvas. Nullable — a page with no stored position is auto-laid-out
-- by the client (grid by order_index) until the user drags it, at which point
-- the position is persisted.
alter table ux_pages add column if not exists canvas_x double precision;
alter table ux_pages add column if not exists canvas_y double precision;
