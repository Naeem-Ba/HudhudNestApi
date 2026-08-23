using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Domain.Enums
{
    public enum PropertyStatus
    {
        Available = 0,
        Reserved = 1,
        Sold = 2,
        Rented = 3,
        Archived = 4,

        /// <summary>
        /// The listing's publication window (see ListingLifecyclePolicy.PublicationPeriod)
        /// elapsed without the owner extending it. Set by ListingExpiryHostedService, never
        /// by a user action.
        ///
        /// Expired is NOT the same as Archived: Archived is an owner's deliberate choice and
        /// is reversible by the owner alone, whereas Expired is time-driven and is reversed
        /// only by a paid extension. Both are hidden from search, but only Expired starts the
        /// grace clock that ends in deletion.
        ///
        /// Stored as the string "Expired" (PropertyConfiguration uses HasConversion&lt;string&gt;
        /// with a 20-char limit), so adding this member needs no data migration.
        /// </summary>
        Expired = 5
    }

}
