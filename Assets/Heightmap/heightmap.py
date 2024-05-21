import requests
from PIL import Image
import numpy as np
import math
from io import BytesIO

# Function to get tile coordinates from lon/lat
def lon_lat_to_tile(lon, lat, zoom):
    lat_rad = math.radians(lat)
    n = 2.0 ** zoom
    xtile = int((lon + 180.0) / 360.0 * n)
    ytile = int((1.0 - math.log(math.tan(lat_rad) + (1 / math.cos(lat_rad))) / math.pi) / 2.0 * n)
    return (xtile, ytile)

def decode_elevation(r, g, b):
    """Decode elevation from RGB values."""
    elevation = -10000 + ((r * 256 * 256 + g * 256 + b) * 0.1)
    return elevation

lon, lat = -0.5585194, 54.2928462
zoom = 18
xtile, ytile = lon_lat_to_tile(lon, lat, zoom)
extent = 4
x_start, x_end = xtile - extent, xtile + extent
y_start, y_end = ytile - extent, ytile + extent

tiles = []
for x in range(x_start, x_end + 1):
    row = []
    for y in range(y_start, y_end + 1):
        url = f'https://api.mapbox.com/v4/mapbox.terrain-rgb/{zoom}/{x}/{y}.png?access_token=pk.eyJ1IjoibXAxNDIzIiwiYSI6ImNsOWVlc2RiMDBlNG8zdXBieW0za253aG8ifQ.7u-NI7ekMuwePbemA1FnrQ'
        response = requests.get(url)
        tile = Image.open(BytesIO(response.content))
        row.append(tile)
    tiles.append(row)

# Stitch tiles together
tile_width, tile_height = tiles[0][0].size
map_width = tile_width * (x_end - x_start + 1)
map_height = tile_height * (y_end - y_start + 1)

heightmap = Image.new('RGB', (map_width, map_height))

for i, row in enumerate(tiles):
    for j, tile in enumerate(row):
        heightmap.paste(tile, (i * tile_width, j * tile_height))

# Convert to grayscale heightmap
heightmap_data = np.array(heightmap)
height_data = np.zeros((heightmap_data.shape[0], heightmap_data.shape[1]))

for i in range(heightmap_data.shape[0]):
    for j in range(heightmap_data.shape[1]):
        r, g, b = heightmap_data[i, j]
        height_data[i, j] = decode_elevation(r, g, b)

heightmap_grayscale = Image.fromarray(height_data).convert('L')
heightmap_grayscale.save('heightmap.png')
