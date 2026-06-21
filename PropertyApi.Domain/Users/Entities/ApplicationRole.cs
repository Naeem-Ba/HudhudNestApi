using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;

namespace PropertyApi.Domain.Users.Entities;

/// <summary>
/// امتداد لـ IdentityRole لإضافة الاسم العربي.
/// لماذا؟ بدلاً من إنشاء جدول Roles جديد يتعارض مع Identity،
/// نضيف فقط NameAr على الـ Role الموجود.
/// EF Core سيضيف عمود NameAr على جدول Roles الموجود بالـ Migration.
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    public string NameAr { get; set; } = string.Empty;

    public ApplicationRole() : base() { }
    public ApplicationRole(string name, string nameAr) : base(name)
    {
        NameAr = nameAr;
    }
}