global using Xunit;
global using Moq;
global using FluentValidation.TestHelper;
global using Microsoft.AspNetCore.Identity;
global using Microsoft.Extensions.Logging;

global using HudhudNestApi.Domain.Auth.Entities;
global using HudhudNestApi.Domain.Enums;
global using HudhudNestApi.Domain.Users.Entities;
global using HudhudNestApi.Application.Auth.DTOs;
global using HudhudNestApi.Application.Auth.Interfaces;
global using HudhudNestApi.Application.Auth.Commands.AddEmail;
global using HudhudNestApi.Application.Auth.Commands.VerifyEmail;
global using HudhudNestApi.Infrastructure.Auth.Services;

global using HudhudNestApi.Auth.Tests.TestHelpers;
