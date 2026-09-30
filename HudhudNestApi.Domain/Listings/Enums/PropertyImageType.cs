using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HudhudNestApi.Domain.Listings.Enums;

public enum PropertyImageType
{
    General,   // عامة
    Exterior,  // واجهة خارجية
    Interior,  // داخلية
    Plan,      // مخطط هندسي
    Document   // وثيقة (صورة عقد أو طابو)
}