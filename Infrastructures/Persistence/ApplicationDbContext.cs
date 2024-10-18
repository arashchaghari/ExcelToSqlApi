using ExcelToSqlApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExcelToSqlApi.Infrastructures.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Person> Persons { get; set; }
    public DbSet<ProcessingStatus> ProcessingStatuses { get; set; }
}