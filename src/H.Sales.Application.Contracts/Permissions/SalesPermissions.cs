namespace H.Sales.Permissions;

public static class SalesPermissions
{
    public const string GroupName = "Sales";

    public static class Customers
    {
        public const string Default = GroupName + ".Customers";
        public const string Create  = Default + ".Create";
        public const string Update  = Default + ".Update";
        public const string Delete  = Default + ".Delete";
    }

    public static class Invoices
    {
        public const string Default = GroupName + ".Invoices";
        public const string Create  = Default + ".Create";
        public const string Update  = Default + ".Update";
        public const string Post    = Default + ".Post";
    }
}
