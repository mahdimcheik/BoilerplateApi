namespace BoilerPlateApi.Utilities
{
    public static class HardCode
    {
        // Roles — stable GUIDs used to seed the RoleApp table.
        public static Guid ROLE_SUPER_ADMIN => Guid.Parse("a0000000-0000-0000-0000-000000000001");
        public static Guid ROLE_ADMIN => Guid.Parse("a0000000-0000-0000-0000-000000000002");
        public static Guid ROLE_CLIENT => Guid.Parse("a0000000-0000-0000-0000-000000000004");

        // Account statuses (StatusAccount lookup table).
        public static Guid ACCOUNT_ACTIVE => Guid.Parse("b0000000-0000-0000-0000-000000000001");
        public static Guid ACCOUNT_PENDING => Guid.Parse("b0000000-0000-0000-0000-000000000002");
        public static Guid ACCOUNT_BANNED => Guid.Parse("b0000000-0000-0000-0000-000000000003");

        // Role names as used by Identity (RoleApp.Name) and JWT role claims.
        public const string ROLE_NAME_SUPER_ADMIN = "SuperAdmin";
        public const string ROLE_NAME_ADMIN = "Admin";
        public const string ROLE_NAME_CLIENT = "Client";
    }

    /// <summary>Roles. Persisted by name in Identity; order is not persisted.</summary>
    public enum RoleEnum
    {
        SuperAdmin,
        Admin,
        Client,
    }

    /// <summary>Account lifecycle status.</summary>
    public enum AccountStatusEnum
    {
        Active,
        Pending,
        Banned,
    }

    /// <summary>How the account authenticates. Persisted as int — append-only.</summary>
    public enum AuthProviderEnum
    {
        Local,
        Google,
    }

    /// <summary>Portfolio media kind. Persisted as int — append-only.</summary>
    public enum MediaTypeEnum
    {
        Image,
        Video,
    }

    /// <summary>Storage backend. Not persisted — used only to pick an IStorageService.</summary>
    public enum StorageProviderEnum
    {
        Local,
        S3,
    }

    /// <summary>
    /// Whether an object is anonymously readable. Encoded as the first segment of every
    /// storage key ("public/..." or "private/..."), so a key alone identifies its visibility.
    /// </summary>
    public enum StorageVisibilityEnum
    {
        Private,
        Public,
    }

}
