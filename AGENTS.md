# H.Sales

Soft Plan Sales API (ABP Modular). Domains: `sales-api` / `sales`.

## Local
- API: https://localhost:44460
- UI: http://localhost:5181 (repo `sales`)
- DB: `H_Sales`
- Auth: https://localhost:44380 · client `H_Sales_App` · audience `H.Sales`

## Commands
```
dotnet run --project src/H.Sales.DbMigrator
dotnet run --project src/H.Sales.HttpApi.Host
```

## Notes
- SoftPlan catalog owns permission names; AppServices use `[Authorize]` only.
- GL: `SalesGl` → H.Accounting via `H_Ops_Machine` (Invoice post).
- Post also creates Inventory Issue (`si:{id}`).
- DbMigrator seeds demo data for every tenant (not only host).
