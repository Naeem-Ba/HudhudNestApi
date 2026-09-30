using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HudhudNestApi.Infrastructure.Media;

public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    public string CloudName { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// Optional root folder prepended to every upload folder (e.g. "staging" turns
    /// "hudhudnest/properties/..." into "staging/hudhudnest/properties/..."). Staging and
    /// Production can share ONE Cloudinary cloud (the free plan has a single product
    /// environment), so this is what actually keeps Staging test uploads out of Production's
    /// folders. Defaults to "staging" in the Staging environment when not configured; see
    /// MediaInfrastructureRegistration.
    /// </summary>
    public string? FolderPrefix { get; set; }
}