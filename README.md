# DateNight

Find ticketed events and free date spots in and around a city, then build them into an itinerary. You can also add your own stops by hand.

## Projects

| Project | What it does |
|---|---|
| `DateNight.Server` | ASP.NET Core API. Hosts the Blazor app, stores accounts and itineraries (SQLite locally, Postgres when deployed), and calls the event and place sources |
| `DateNight.Client` | Blazor WebAssembly UI (Explore, My itineraries, Sign in) |
| `DateNight.Shared` | Data types shared by the client and server |

## Data sources

- **Ticketmaster Discovery API**: ticketed events. Needs a free key.
- **Google Places API (New)**: restaurants (with ratings, price level and photos) and parks. Needs a key and billing turned on.
- **OpenTable**: each restaurant gets a **Reserve** link that opens OpenTable's search for it, prefilled for 2 people. OpenTable's own API is only open to approved partners.
- **OpenStreetMap**: Nominatim turns a city name into coordinates. Overpass covers arts & culture and nightlife, and stands in for Google when no Google key is set. No key needed.

## First-time setup

1. Install the **.NET 10 SDK**. `dotnet --list-sdks` should show a `10.0.x` version.
2. Get a Ticketmaster key: sign up at https://developer.ticketmaster.com, open *My Apps*, and copy the **Consumer Key**.
3. Store the key as a user secret. The key stays off disk in the project and out of git:

   ```
   cd DateNight.Server
   dotnet user-secrets set "Ticketmaster:ApiKey" "YOUR_CONSUMER_KEY"
   ```

4. Get a Google Places key (optional, but much better restaurant and park results):
   1. In the [Google Cloud console](https://console.cloud.google.com/), create a project and turn on billing.
   2. Enable **Places API (New)** under *APIs & Services > Library*.
   3. Create an API key under *APIs & Services > Credentials*. Under *API restrictions*, limit it to Places API (New).
   4. Store it:

      ```
      dotnet user-secrets set "GooglePlaces:ApiKey" "YOUR_GOOGLE_KEY"
      ```

   Google bills per request, and each billing category comes with a monthly free allowance. Results are cached on the server for an hour to keep repeat searches free.

5. Identify the app to OpenStreetMap (needed for parks, culture and nightlife results). OpenStreetMap's free servers refuse requests that don't say who is sending them. Add a contact email:

   ```
   dotnet user-secrets set "OpenStreetMap:ContactEmail" "you@example.com"
   ```

   If the main Overpass server is overloaded, the app automatically tries the backup servers listed under `OpenStreetMap:OverpassUrls` in `appsettings.json`.

## Run it

```
cd DateNight.Server
dotnet watch
```

The app opens at http://localhost:5275. Run the **Server** project. It serves the client too, so you don't need to run the client project separately.

In VS Code, pick **DateNight.Server** when C# Dev Kit asks which project to run or debug.

The first run creates `datenight.db` next to the server. Delete that file to reset all data.

## Deploy to Render (free)

The app runs on Render as a Docker container, with the data in a free Neon Postgres database. Render's free disk is wiped whenever the app restarts, so the database has to live elsewhere.

**1. Create the database (Neon, free)**

1. Sign up at https://neon.com and create a project.
2. On the project dashboard, click **Connect** and copy the connection string. It looks like `postgresql://user:password@ep-xxxx.neon.tech/neondb?sslmode=require`.

The app creates its tables the first time it starts.

**2. Push the code to GitHub**

Commit and push to `master` on https://github.com/Reesejones1989/DateNight. The repo already contains `Dockerfile` and `render.yaml`.

**3. Create the service on Render**

1. In the Render dashboard, choose **New > Blueprint** and connect the GitHub repo.
2. Render reads `render.yaml` and asks for these values:

   | Variable | Value |
   |---|---|
   | `ConnectionStrings__Default` | The Neon connection string from step 1 |
   | `Ticketmaster__ApiKey` | Your Ticketmaster key |
   | `GooglePlaces__ApiKey` | Your Google key (leave blank to use OpenStreetMap only) |
   | `OpenStreetMap__ContactEmail` | Your email, for OpenStreetMap's usage policy |

3. Click **Apply**. The first build takes about 5–10 minutes. Your site is then at `https://datenight-xxxx.onrender.com`.

Every push to `master` redeploys automatically. To change a key later, go to the service's **Environment** tab.

**Free-tier behavior**

- The site sleeps after 15 minutes without visitors. The next visit takes about a minute to wake it up.
- Neon also pauses the database after 5 idle minutes; the first query after that takes a second or two longer.
- Sign-ins survive restarts because the sign-in keys are stored in the database.
- OpenStreetMap's main server blocks some hosting providers. If Arts & Culture or Nightlife searches fail on Render but work on your computer, that's why. The app tries the backup servers automatically, and Google covers restaurants and parks either way.

## API

| Method | Route | Notes |
|---|---|---|
| POST | `/account/register` | `{ email, password }` |
| POST | `/account/login?useCookies=true` | `{ email, password }` |
| POST | `/account/logout` | |
| GET | `/account/me` | Current user or 401 |
| GET | `/api/search/events?city=&radius=&from=&to=&keyword=&category=` | Ticketmaster |
| GET | `/api/search/places?city=&radius=&category=outdoors\|culture\|food\|nightlife&date=` | Google Places (food, outdoors) or OpenStreetMap |
| GET | `/api/photos?name=` | Redirects to a Google place photo |
| GET/POST | `/api/itineraries` | List / create |
| GET/PUT/DELETE | `/api/itineraries/{id}` | |
| POST | `/api/itineraries/{id}/items` | Add a search result or a manual stop |
| PUT/DELETE | `/api/itineraries/{id}/items/{itemId}` | |

## Next ideas

- Switch from `EnsureCreated` to EF Core migrations once the data model settles (`dotnet tool install -g dotnet-ef`)
- Share an itinerary with your date via a read-only link
- Show stops on a map
- Add more event sources (SeatGeek, local venue calendars)
