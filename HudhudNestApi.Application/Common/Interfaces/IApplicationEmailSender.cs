using System.Threading;
using System.Threading.Tasks;
using HudhudNestApi.Application.Common.Models;

namespace HudhudNestApi.Application.Common.Interfaces
{

    public interface IApplicationEmailSender
    {
        Task SendEmailAsync(
            string email,
            string subject,
            string htmlMessage);

        /// <summary>
        /// Sends a message that may carry a plain-text alternative alongside the HTML.
        /// Senders that cannot use the text part ignore it.
        /// </summary>
        Task SendEmailAsync(
            EmailMessage message,
            CancellationToken ct = default);
    }
}
