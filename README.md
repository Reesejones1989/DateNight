# DateNight

Find ticketed events and free date spots in and around a city, then build them into an itinerary. You can also add your own stops by hand.

## Projects

| Project | What it does |
|---|---|
| `DateNight.Server` | ASP.NET Core API. Hosts the Blazor app, stores accounts and itineraries in SQLite, and calls the event and place sources |
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

## Run it

```
cd DateNight.Server
dotnet watch
```

The app opens at http://localhost:5275. Run the **Server** project. It serves the client too, so you don't need to run the client project separately.

In VS Code, pick **DateNight.Server** when C# Dev Kit asks which project to run or debug.

The first run creates `datenight.db` next to the server. Delete that file to reset all data.

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
