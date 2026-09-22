using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HudhudNestApi.Domain.Common.Interfaces
{
    internal interface IHasDomainEvents
    {
        // Marker interface reserved for a future domain-events pattern (an entity that
        // raises events other parts of the system react to after SaveChanges). No entity in
        // this codebase implements it yet, and there is no DomainEvent base type or dispatch
        // pipeline behind it today -- it is currently unused. The original comment here was
        // corrupted to literal '?' characters (a non-UTF-8 save) and could not be recovered
        // from source control; this describes the interface's actual (empty) contract rather
        // than guessing at lost wording.
    }
}
