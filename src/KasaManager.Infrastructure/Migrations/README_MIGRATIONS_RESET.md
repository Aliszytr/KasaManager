# EF Core Migrations (SQL Server) — Clean Reset Baseline

Bu proje **SQL Server ENFORCED** çalışır (SQLite fallback yoktur).

## Neden bu klasör boş gelebilir?
Bu zip, "SQLite → SQL Server" geçişinde yaşanan **eski snapshot/designer kalıntıları** sebebiyle EF Core'un yanlış migration üretmesini engellemek için **clean reset** yaklaşımıyla hazırlanmıştır.

- Uygulama **Development** ortamında migration yoksa `EnsureCreated()` ile **dev bootstrap** yapabilir.
- Production / canlı ortamda ise migration zorunludur.

## Tek Komut: Migration + DB Update
Repo root'tan:

```powershell
scripts\01_install_dotnet_ef.ps1
scripts\02_migrate_database.ps1
```

## Hard Reset (Migration geçmişini sıfırla)

```powershell
scripts\03_reset_migrations.ps1
```

Bu script mevcut migration dosyalarını siler, yeni bir baseline migration oluşturur ve `Update-Database` çalıştırır.
