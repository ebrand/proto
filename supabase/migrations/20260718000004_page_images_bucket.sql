-- Storage bucket for UX-page mockup images. Public read (images are served via
-- their public CDN URL, stored in ux_pages.image_url); writes only happen
-- through API-issued signed upload URLs, so no RLS upload policies are needed.
-- Size + mime limits mirror what the API accepts.
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('page-images', 'page-images', true, 10485760,
        array['image/png', 'image/jpeg', 'image/webp'])
on conflict (id) do nothing;
