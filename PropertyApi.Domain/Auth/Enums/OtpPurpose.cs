using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Domain.Auth.Enums
{
    /// <summary>
/// الغرض من رمز OTP
/// كل نوع عملية يستخدم رمزاً منفصلاً لأسباب أمنية
/// </summary>
public enum OtpPurpose
{
    /// <summary>تسجيل مستخدم جديد برقم الهاتف</summary>
    PhoneRegistration = 1,
 
    /// <summary>تسجيل دخول مستخدم موجود برقم الهاتف</summary>
    PhoneLogin = 2,
 
    /// <summary>إضافة رقم هاتف لحساب موجود</summary>
    AddPhoneToAccount = 3,
 
    /// <summary>تغيير رقم الهاتف</summary>
    ChangePhone = 4,
}
}
