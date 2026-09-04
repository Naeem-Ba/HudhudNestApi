namespace PropertyApi.Application.Investments.DTOs;

public sealed record InvestmentDocumentUploadFileDto(
    Stream Content,
    string FileName,
    string ContentType,
    long Length);
