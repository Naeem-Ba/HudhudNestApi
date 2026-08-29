using MediatR;

namespace PropertyApi.Application.Services.Commands.UploadServiceRequestDocument;

/// <summary>One file (image only this vertical slice — see handler remarks) attached to a
/// ServiceRequest as a deliverable/report/reference photo.</summary>
public sealed record UploadServiceRequestDocumentCommand(
    Guid ServiceRequestId,
    Guid ActorUserId,
    Stream Content,
    string FileName,
    string ContentType,
    long Length) : IRequest<Application.Services.DTOs.ServiceRequestDocumentDto>;
