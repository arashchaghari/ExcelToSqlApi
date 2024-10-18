namespace ExcelToSqlApi.Entities;

public class Person
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public DateTime DateCreated { get; set; }
}
