global using Xunit;
global using Moq;
global using FluentValidation.TestHelper;
global using Microsoft.AspNetCore.Identity;
global using Microsoft.Extensions.Logging;

global using PropertyApi.Domain.Auth.Entities;
global using PropertyApi.Domain.Enums;
global using PropertyApi.Domain.Users.Entities;
global using PropertyApi.Application.Auth.DTOs;
global using PropertyApi.Application.Auth.Interfaces;
global using PropertyApi.Application.Auth.Commands.SendPhoneOtp;
global using PropertyApi.Application.Auth.Commands.VerifyPhoneOtp;
global using PropertyApi.Application.Auth.Commands.AddEmail;
global using PropertyApi.Application.Auth.Commands.VerifyEmail;
global using PropertyApi.Infrastructure.Auth.Services;

global using PropertyApi.Auth.Tests.TestHelpers;
