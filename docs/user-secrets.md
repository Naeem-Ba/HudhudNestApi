# User Secrets Setup

This document explains how to configure local development secrets for the PropertyApi backend.

## Purpose

Sensitive values must not be committed to Git.

Examples of sensitive values:

- Database passwords
- JWT signing keys
- Cloudinary credentials
- Admin seed passwords
- API secrets

For local development, use the .NET User Secrets store.

## Initialize User Secrets

Run this command from the repository root:

```powershell
dotnet user-secrets init --project .\PropertyApi\PropertyApi.csproj