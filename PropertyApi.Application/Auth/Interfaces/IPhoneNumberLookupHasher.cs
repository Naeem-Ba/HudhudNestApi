using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Application.Auth.Interfaces;

public interface IPhoneNumberLookupHasher
{
    string Compute(string phoneNumber);
}
