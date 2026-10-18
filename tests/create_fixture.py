from pathlib import Path
import cv2
import numpy as np

root = Path(__file__).resolve().parents[1] / "test-results"
root.mkdir(exist_ok=True)
path = root / "frame-test.mp4"
writer = cv2.VideoWriter(str(path), cv2.VideoWriter_fourcc(*"mp4v"), 30, (640, 360))
assert writer.isOpened()
for frame in range(1800):
    image = np.zeros((360, 640, 3), dtype=np.uint8)
    image[:, :, 0] = np.linspace(40, 100, 640).astype(np.uint8)
    image[:, :, 1] = np.linspace(25, 65, 360).astype(np.uint8)[:, None]
    image[:, :, 2] = 28
    cv2.putText(image, "FRAME ACCURACY TEST", (44, 90), cv2.FONT_HERSHEY_SIMPLEX, 1, (230, 230, 230), 2)
    cv2.putText(image, f"Frame {frame:04d} / 1800", (44, 170), cv2.FONT_HERSHEY_SIMPLEX, 1.25, (255, 183, 103), 2)
    cv2.putText(image, f"640 x 360  |  30 fps  |  {frame / 30:05.2f}s", (44, 230), cv2.FONT_HERSHEY_SIMPLEX, .68, (190, 190, 190), 1)
    cv2.circle(image, (70 + frame % 500, 290), 12, (255, 183, 103), -1)
    writer.write(image)
writer.release()
print(path)
