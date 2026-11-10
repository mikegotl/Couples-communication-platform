# Setting up Google and Apple sign-in

Both are optional. Each button appears only when all of its settings are present, so the app
runs exactly as before until you add them. Settings never go in the repository: use
`dotnet user-secrets` locally and Key Vault (or Container App secrets) when hosted.

The callback paths the app listens on are `/signin-google` and `/signin-apple`.

## Google (free, about 10 minutes)

1. Open <https://console.cloud.google.com/>, create a project (for example "BridgeTogether").
2. Go to **Google Auth Platform** (APIs & Services, OAuth consent screen). Fill in **Branding**
   (app name, support email), set **Audience** to External, and while you're testing add your
   own Google address under **Test users**. The app only asks for `openid`, `email`, `profile`.
3. Go to **Clients**, **Create client**, type **Web application**. Under **Authorized redirect
   URIs** add:
   - `http://localhost:5180/signin-google` (local; Google allows plain http for localhost)
   - `https://<your domain>/signin-google` (once the app has a public address)
4. Copy the client ID and client secret, then from `src/Couples.Web`:

   ```bash
   dotnet user-secrets set "Authentication:Google:ClientId" "<client id>"
   dotnet user-secrets set "Authentication:Google:ClientSecret" "<client secret>"
   ```

5. Restart the app. "Continue with Google" appears on Log in and Create account.

Before real participants use it, publish the app in Google Auth Platform (Audience, Publish).
Google may ask to verify the app's domain and privacy notice.

## Apple (needs a paid Apple Developer Program membership)

Apple only redirects to **https** addresses on a real domain, never `localhost`. Test it on the
hosted app, or through an https tunnel to your Mac (for example Cloudflare Tunnel or ngrok).

1. In <https://developer.apple.com/account/resources/identifiers/list>, add an **App ID** (type
   App) with **Sign in with Apple** ticked. This is the "primary App ID"; it needs no app.
2. Add a **Services ID**, for example `com.bridgetogether.web`. This value is the `ClientId`.
   Tick **Sign in with Apple**, press **Configure**, choose the primary App ID, and enter:
   - Domains: `<your domain>` (no https://)
   - Return URLs: `https://<your domain>/signin-apple`
3. Under **Keys**, add a key with **Sign in with Apple** ticked (configure it with the same
   primary App ID). Download the `.p8` file; Apple only lets you download it once. Note its
   **Key ID**.
4. Your **Team ID** is shown at the top right of the developer site (Membership details).
5. From `src/Couples.Web`:

   ```bash
   dotnet user-secrets set "Authentication:Apple:ClientId" "com.bridgetogether.web"
   dotnet user-secrets set "Authentication:Apple:TeamId" "<team id>"
   dotnet user-secrets set "Authentication:Apple:KeyId" "<key id>"
   dotnet user-secrets set "Authentication:Apple:PrivateKeyPath" "/full/path/to/AuthKey_<key id>.p8"
   ```

   When hosted, put the contents of the `.p8` file in `Authentication:Apple:PrivateKey`
   instead (a Key Vault secret named `Authentication--Apple--PrivateKey`).

People who choose "Hide my email" get an `@privaterelay.appleid.com` address. Mail to it only
arrives once the sending domain is registered under **Sign in with Apple for Email
Communication** in the developer site; do that when the email provider is chosen.

## Hosted settings

| Setting | Key Vault secret name |
|---|---|
| `Authentication:Google:ClientId` | `Authentication--Google--ClientId` |
| `Authentication:Google:ClientSecret` | `Authentication--Google--ClientSecret` |
| `Authentication:Apple:ClientId` | `Authentication--Apple--ClientId` |
| `Authentication:Apple:TeamId` | `Authentication--Apple--TeamId` |
| `Authentication:Apple:KeyId` | `Authentication--Apple--KeyId` |
| `Authentication:Apple:PrivateKey` | `Authentication--Apple--PrivateKey` |

The Azure Terraform doesn't create these yet; add them alongside the existing app secrets when
the staging environment is approved.
