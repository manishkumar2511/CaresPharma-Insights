namespace CaresPharma_Insights.UI.Models
{
    public class UserProfileModel
    {
        public string? Id { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Role { get; set; }
        public string? ProfileImageUrl { get; set; }
        public string? PharmacyOwnerId { get; set; }

        public string FullName
        {
            get
            {
                string name = $"{FirstName} {LastName}".Trim();
                return string.IsNullOrWhiteSpace(name) ? (Email ?? "User") : name;
            }
        }

        public string Initials
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(FirstName) && !string.IsNullOrWhiteSpace(LastName))
                {
                    return $"{FirstName[0]}{LastName[0]}".ToUpper();
                }
                if (!string.IsNullOrWhiteSpace(FirstName))
                {
                    return FirstName.Substring(0, Math.Min(2, FirstName.Length)).ToUpper();
                }
                if (!string.IsNullOrWhiteSpace(Email))
                {
                    return Email.Substring(0, Math.Min(2, Email.Length)).ToUpper();
                }
                return "CP";
            }
        }
    }
}
