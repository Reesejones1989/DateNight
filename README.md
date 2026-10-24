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


