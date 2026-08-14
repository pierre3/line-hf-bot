# certs/

Drop extra CA certificates here (PEM-encoded `*.crt`) to have the Docker image trust them —
useful behind a corporate TLS-inspecting proxy so `dotnet restore` and outbound HTTPS
(Hugging Face / LINE) work from inside the container.

Empty by default (this is a no-op on normal networks). `*.crt` files are gitignored.

Export your host's root CAs (PowerShell) if you need them:
```powershell
New-Item -ItemType Directory -Force certs | Out-Null
Get-ChildItem Cert:\LocalMachine\Root | ForEach-Object {
  $pem = "-----BEGIN CERTIFICATE-----`n" +
         [Convert]::ToBase64String($_.RawData, 'InsertLineBreaks') +
         "`n-----END CERTIFICATE-----"
  Set-Content -Path "certs/root_$($_.Thumbprint).crt" -Value $pem -Encoding ascii
}
```
