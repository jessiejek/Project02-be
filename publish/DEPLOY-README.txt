ClinicApp.Api — IIS deploy bundle
=================================
Built: framework-dependent, win-x64, in-process hosting.
Target: Windows Server + IIS with the .NET 10 ASP.NET Core Hosting Bundle.

WHAT TO UPLOAD (FileZilla)
-------------------------
Drag the ENTIRE contents of this folder into the site's physical path
(e.g. C:\inetpub\wwwroot\clinicapi\), EXCEPT the two helper files:
  - DEPLOY-README.txt        (this file)
  - _migrations-idempotent.sql  (run once against the DB, see below)

Before uploading over a running site, drop a file named  app_offline.htm
in the site root first (any content). IIS then releases the DLLs so the
upload doesn't fail on locked files. Delete it when you're done.

ON THE SERVER — ONE TIME
------------------------
1. Install the ".NET 10  Hosting Bundle" (the Hosting Bundle, not just the
   runtime — it adds the IIS AspNetCoreModuleV2). Then: net stop was /y && net start w3svc
2. App Pool for this site: .NET CLR version = "No Managed Code",
   and set it to run as an identity that can reach SQL Server.
3. Create  appsettings.Production.json  in the site root (do NOT commit it):

   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=YOUR_SQL_HOST;Database=ClinicAppDb;User Id=...;Password=...;TrustServerCertificate=True;Encrypt=True"
     },
     "Jwt": { "Secret": "<a long random production secret>" },
     "Cors": { "AllowedOrigins": [ "https://your-frontend-domain" ] }
   }

   (Check appsettings.json for the exact key names this build expects and
   copy any others you need to override.)
4. Make sure the app process env has  ASPNETCORE_ENVIRONMENT=Production.
   Either a system env var, or add to web.config inside <aspNetCore>:
     <environmentVariables>
       <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
     </environmentVariables>
5. Create an empty  logs  folder in the site root (web.config writes stdout
   there if you flip stdoutLogEnabled="true" to debug a boot failure).

DATABASE
--------
Run  _migrations-idempotent.sql  once against the production ClinicAppDb
(SSMS or sqlcmd). It is safe to re-run — it only applies migrations that
aren't already recorded in __EFMigrationsHistory. It brings the schema up
to and including 20260910130827_Phase93ConsultationPfDecision.

VERIFY
------
Browse  https://your-api-domain/api/settings  — should return 200 JSON.
If you get HTTP 500.30/500.31/502.5, set stdoutLogEnabled="true" in
web.config, hit the site again, and read logs\stdout_*.log.
