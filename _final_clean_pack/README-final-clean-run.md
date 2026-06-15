# Final clean run for OTP/SMS/ForwardedHeaders tests

This pack fixes the stale DLL issue caused by ZIP entry timestamps. The PowerShell script copies the corrected test files, removes all `bin` and `obj` folders, sets test-only environment variables needed by Production-mode forwarded-header tests, then runs restore/build/test.

Run from the repository root:

```powershell
Expand-Archive "$env:USERPROFILE\Downloads\propertyapi-otp-tests-final-clean-pack.zip" -DestinationPath ".\_final_clean_pack" -Force
.\_final_clean_pack\Apply-Final-OtpSecurityTestsFix.ps1 -RepoRoot "C:\Users\naeem\Source\Repos\PropertyApi"
```
