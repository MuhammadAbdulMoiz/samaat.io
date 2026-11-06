# Try Samaat on your laptop and phone

The phone app has its own repo, **[samaat-mobile](https://github.com/MuhammadAbdulMoiz/samaat-mobile)**. Its guide, [TRY-IT.md](https://github.com/MuhammadAbdulMoiz/samaat-mobile/blob/main/docs/TRY-IT.md), covers all of it: this desktop app, the phone app in a window on your laptop, the APK on an Android phone, and why there's no iPhone app.

## The desktop app on its own

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then:

```bash
git clone https://github.com/MuhammadAbdulMoiz/samaat.io.git
cd samaat.io
git checkout dev   # where new work lands; master holds releases

# Windows, your real paired earbuds
dotnet run --project src/Samaat.App -f net10.0-windows10.0.19041.0

# Windows, real earbuds plus three simulated ones (use this to pair with the phone preview)
dotnet run --project src/Samaat.App -f net10.0-windows10.0.19041.0 -- --simulate
```

On a Mac or Linux laptop, `dotnet run --project src/Samaat.App -f net10.0` runs it with simulated earbuds only.

To pair a phone: **Settings → Phone → Pair a phone**, and allow Samaat on private networks when Windows asks.
