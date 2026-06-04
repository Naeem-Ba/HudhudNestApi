using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Domain.Enums
{
    public enum HeatingType
    {
        Unknown = 0,
        Central = 1,
        Gas = 2,
        Electric = 3,
        FloorHeating = 4,
        Oil = 5,
        Fireplace = 6,
        Solar = 7,
        HeatPump = 8
    }
}
