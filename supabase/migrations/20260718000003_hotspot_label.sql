-- Hotspots need a human label so they can name a CTA — essential for functional
-- prototypes, where the page is a live app (no Proto-owned image to draw a
-- region on) and the link is identified by the call-to-action's name rather
-- than by geometry. Nullable: illustrative rect hotspots may leave it unset.
alter table hotspots add column if not exists label text;
