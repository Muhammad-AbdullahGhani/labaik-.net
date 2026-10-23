# Reproduce the 200-user checks

Run from the repository root with .NET 10 and Docker running:

```powershell
dotnet test tests/Labaik.IntegrationTests --configuration Release --filter FullyQualifiedName~LoadTests
```

NBomber starts 200 scenario copies and a barrier releases all 200 operations together. There is no warm-up. Testcontainers creates isolated PostgreSQL with restricted application/gateway roles; the production API endpoints run through ASP.NET's HTTP TestServer, including real JWT/signature verification. Gateway HTTP serialization uses its separate application. This is a local correctness load proof, not a public-network throughput benchmark.

- Capacity: 200 signed HTTP payment callbacks and independently scoped capture processors; assert 200 captures, 50 allocations and 150 durable refund requests.
- Voucher global: 200 JWT-authenticated HTTP checkout requests, distinct users; assert exactly 20 reservations.
- Voucher per user: 200 JWT-authenticated HTTP checkout requests, same user; assert exactly 2 reservations.

The harness issues test JWTs with random test-only signing keys and seeded users. It adds no production login bypass. Unexpected responses fail the test; expected voucher conflicts count as correct responses. Database assertions prove the business limits after load completes. Reports go to the OS temporary directory under `labaik-load-reports`; no tokens, payment payloads or signed file links are written. NBomber 6 is used for this personal take-home project; organization use requires its applicable license (https://nbomber.com/docs/getting-started/license/).
