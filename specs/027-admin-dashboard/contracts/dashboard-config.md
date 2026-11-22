# Contract: Dashboard configuration

Section `Dashboard` in `appsettings.json` (all optional; no secrets):

```json
"Dashboard": {
  "DefaultRangeDays": 30,
  "MaxRangeDays": 366,
  "DefaultTopProducts": 5,
  "MaxTopProducts": 20,
  "CacheSeconds": 60
}
```

| Key | Default | Effective value rule |
|---|---|---|
| `DefaultRangeDays` | 30 | `< 1` or `> MaxRangeDays` -> 30 |
| `MaxRangeDays` | 366 | `< 1` -> 366 (hard ceiling 3660 to bound the daily series) |
| `DefaultTopProducts` | 5 | `< 1` or `> MaxTopProducts` -> 5 |
| `MaxTopProducts` | 20 | `< 1` -> 20 (hard ceiling 100) |
| `CacheSeconds` | 60 | `0` disables the cache; negative -> 60 |

- `DashboardOptions` exposes `EffectiveDefaultRangeDays`, `EffectiveMaxRangeDays`, `EffectiveDefaultTopProducts`, `EffectiveMaxTopProducts`, `EffectiveCacheSeconds` (pattern of `InventoryOptions.Effective`); the controller and service read only the effective values; invalid configuration never throws (unlike `Payments:Provider`, a wrong number is harmless).
- Environment override form: `Dashboard__CacheSeconds=0` etc.; optional lines in `.env.example`/`docker-compose.yml` are NOT required (defaults are fine) and are not added (YAGNI).
- Tests: `ApiFactory` sets `Dashboard:CacheSeconds=0`; `DashboardOptionsTests` cover the table above.
- The MVC mirrors only the user-facing limits as constants (`MaxRangeDays = 366`, `MaxTop = 20`) for its pre-validation; API answers stay authoritative.
