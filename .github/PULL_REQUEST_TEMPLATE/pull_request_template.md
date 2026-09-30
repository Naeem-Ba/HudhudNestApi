## Summary

Describe the purpose of this pull request.

## Changes

- 
- 
- 

## Validation

- [ ] `dotnet format HudhudNestApi.sln --verify-no-changes --no-restore`
- [ ] `dotnet build HudhudNestApi.sln --configuration Release --no-restore`
- [ ] `dotnet test HudhudNestApi.sln --configuration Release --no-build`
- [ ] CI quality gates reviewed: vulnerability scan, coverage baseline, artifacts.

## Checklist

- [ ] No secrets or local configuration files are committed.
- [ ] No `bin/`, `obj/`, `.vs/`, or runtime uploads are committed.
- [ ] Tests were added or updated where appropriate.
- [ ] API behavior was verified if controllers/endpoints changed.
- [ ] Database/migration impact was reviewed.
- [ ] No quality gate was skipped without documenting why.
