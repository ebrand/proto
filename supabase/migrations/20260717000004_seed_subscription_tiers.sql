-- Seed the platform subscription-tier catalog (reference data). Idempotent:
-- re-running leaves existing rows untouched. Prices/limits are initial
-- placeholders, safe to tune later.
insert into subscription_tiers (code, name, max_seats, max_prototypes, monthly_price_cents, currency)
values
  ('free',       'Free',        3,   3,       0, 'USD'),
  ('pro',        'Pro',        25,   null,  4900, 'USD'),
  ('enterprise', 'Enterprise', 500,  null, 49900, 'USD')
on conflict (code) do nothing;
