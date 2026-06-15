using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Application.Common.Interfaces
{

    public interface IApplicationEmailSender
    {
        Task SendEmailAsync(
            string email,
            string subject,
            string htmlMessage);
    }
}

