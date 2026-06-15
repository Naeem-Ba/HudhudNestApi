# Final clean run pack v5

This pack fixes the remaining Production-mode integration test startup error by providing a test-only CORS allowed origin through environment variables before `Program.cs` configures CORS.

Run:

```powershell
cd C:\Users\naeem\Source\Repos\PropertyApi
Expand-Archive "$env:USERPROFILE\Downloads\propertyapi-otp-tests-final-cors-pack.zip" -DestinationPath ".\_final_cors_pack" -Force
Set-ExecutionPolicy -Scope Process Bypass -Force
.\_final_cors_pack\Apply-Final-OtpSecurityTestsFix.ps1 -RepoRoot "C:\Users\naeem\Source\Repos\PropertyApi"
```
