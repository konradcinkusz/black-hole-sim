# CSharp FlashCards 2025

[![PDF download](https://img.shields.io/badge/PDF-download-blue)](https://github.com/konradcinkusz/csharp-flashcards/releases/latest/download/CSharp_FlashCards.pdf)
[![CI](https://github.com/konradcinkusz/csharp-flashcards/actions/workflows/ci.yml/badge.svg)](https://github.com/konradcinkusz/csharp-flashcards/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.txt)

# BlackHoleSim

A C# implementation of a Schwarzschild black hole raytracer.
This project numerically integrates photon geodesics in general relativity and renders a synthetic 
image of a black hole with a thin accretion disk.

---

## ✨ Features
- Photon geodesics in Schwarzschild spacetime (`G = c = 1` units).
- Hamiltonian formulation of null geodesics with conserved energy and angular momentum.
- Runge–Kutta 4 integrator for stable numerical integration.
- Ray-marching renderer to generate images in `.ppm` format.
- Console progress bar for rendering feedback.
- Modular solution layout (core physics library + console app).

---

## 📂 Folder Structure

```

BlackHoleSim.sln
├─ BlackHoleSim.Core/                 # Core physics + rendering library
│  ├─ Physics/
│  │   ├─ State.cs
│  │   ├─ IMetric.cs
│  │   ├─ Schwarzschild.cs
│  │
│  ├─ Math/
│  │   └─ RK4.cs
│  │
│  ├─ Rendering/
│  │   ├─ Raytracer.cs
│  │   └─ PPMWriter.cs
│  │
│  └─ BlackHoleSim.Core.csproj
│
├─ BlackHoleSim.ConsoleApp/           # Console front-end
│  ├─ Program.cs
│  ├─ UI/
│  │   └─ ConsoleProgressBar.cs
│  │
│  └─ BlackHoleSim.ConsoleApp.csproj
│
└─ BlackHoleSim.sln

````

---

## 🚀 Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download) or newer  
- (Optional) [ImageMagick](https://imagemagick.org/) for converting `.ppm` output to `.png`

### Build
```bash
dotnet build
````

### Run

```bash
dotnet run --project BlackHoleSim.ConsoleApp
```

This produces an image file:

```
blackhole.ppm
```

Convert to PNG/JPG with ImageMagick:

```bash
magick blackhole.ppm blackhole.png
```

---

## ⚙ Configuration

You can adjust rendering parameters in `Raytracer.cs`:

* `Rin`, `Rout` → accretion disk inner/outer radius (default: 6–20M).
* `Rcam` → camera distance (default: 50M).
* `Step` → integration step size (smaller = more accurate, slower).
* `Width`, `Height` → image resolution.
* `bMax` → field of view scaling (impact parameter range).

---

## 🖼 Example Output

Example rendering of a Schwarzschild black hole with a thin orange disk:

<img src="docs/example_blackhole.png" width="400"/>

---

## 🧮 Theory (Brief)

* We use the Schwarzschild metric (equatorial plane):

  $$
  ds^2 = -\left(1 - \frac{2M}{r}\right)dt^2
  + \frac{dr^2}{1 - 2M/r} + r^2 d\phi^2
  $$

* Photon motion is derived from the Hamiltonian:

  $$
  H = \tfrac{1}{2} g^{\mu\nu} p_\mu p_\nu = 0
  $$

* Integration is done via Runge–Kutta 4 (RK4).

* The event horizon is at \$r = 2M\$.

* The innermost stable circular orbit (ISCO) for the disk is at \$r = 6M\$.

---

## 📚 References

* Sean Carroll – *Spacetime and Geometry* (2003)
* J.-P. Luminet (1979) – *Image of a spherical black hole with thin accretion disk*
* Kavan’s video: *Simulating Black Holes in C++* (YouTube)
* Kip Thorne – *Black Holes and Time Warps* (1994)

---

## 🛠 Future Work

* Extend to Kerr metric (rotating black holes).
* More realistic disk shading (gravitational redshift, Doppler beaming).
* Parallelized rendering (`Parallel.For`).
* GUI front-end.

---

## 📜 License

This project is licensed under the MIT License.

---

## 📐 Designs

### Class Diagram

```mermaid
classDiagram
direction LR

class State {
  +double t
  +double r
  +double phi
  +double pt
  +double pr
  +double pphi
  +State AddScaled(State k, double a)
  +operator+(State, State)
  +operator*(double, State)
}

class IMetric {
  <<interface>>
  +double H(State s)
  +State RHS(State s)
}

class Schwarzschild {
  <<implements IMetric>>
  +const double M
  +double rs
  +double gttInv(double r)
  +double grrInv(double r)
  +double gppInv(double r)
  +double dgttInv_dr(double r)
  +double dgrrInv_dr(double r)
  +double dgppInv_dr(double r)
  +double H(State s)
  +State RHS(State s)
}

class RK4 {
  <<static>>
  +State Step(Func<State,State> f, State y, double h)
}

class Raytracer {
  <<static>>
  -const double Rin
  -const double Rout
  -const double Rcam
  -const double Step
  -const int    MaxSteps
  +(byte r,byte g,byte b) Trace(double bImpact)
  +void RenderPPM(string path, int width, int height, double bMax)
}

class PPMWriter {
  <<static>>
  +void WriteHeader(StreamWriter sw, int width, int height)
  +void WritePixel(StreamWriter sw, byte r, byte g, byte b)
}

class ConsoleProgressBar {
  +void Reset()
  +void Report(double fraction)
  +void Complete()
}

class Program {
  +static void Main(string[] args)
}

IMetric <|.. Schwarzschild
RK4 ..> State : integrates
Raytracer ..> RK4 : uses
Raytracer ..> Schwarzschild : metric
Raytracer ..> PPMWriter : output
Program ..> Raytracer : orchestrates
Program ..> ConsoleProgressBar : progress UI
```

### Component / Package Diagram

```mermaid
flowchart LR
  subgraph Core["BlackHoleSim.Core"]
    Physics["Physics\n(State, IMetric, Schwarzschild)"]
    Math["Math\n(RK4)"]
    Rendering["Rendering\n(Raytracer, PPMWriter)"]
  end

  subgraph Console["BlackHoleSim.ConsoleApp"]
    UI["UI\n(ConsoleProgressBar)"]
    Entry["Program.cs"]
  end

  Entry --> UI
  Entry --> Rendering
  Rendering --> Physics
  Rendering --> Math
```

### Sequence Diagram (Rendering)

```mermaid
sequenceDiagram
  autonumber
  participant Prog as Program
  participant Bar as ConsoleProgressBar
  participant Ray as Raytracer
  participant RK as RK4
  participant Met as Schwarzschild
  participant PPM as PPMWriter

  Prog->>Bar: Reset()
  Prog->>Ray: RenderPPM(path, W, H, bMax)
  Ray->>PPM: WriteHeader(W,H)

  loop for each row j
    Ray->>Bar: Report((j+1)/H)
    loop for each pixel i
      Ray->>Ray: Trace(bImpact)
      Ray->>Met: RHS(state)
      Met-->>Ray: d(state)/dλ
      Ray->>RK: Step(RHS, state, h)
      RK-->>Ray: state'
      Ray->>PPM: WritePixel(r,g,b)
    end
  end

  Prog->>Bar: Complete()
```
