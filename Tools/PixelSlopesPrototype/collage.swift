import AppKit
// collage.swift <out.png> <title> <label1> <img1> <label2> <img2> ...
let args = CommandLine.arguments
let out = args[1]; let title = args[2]
var items: [(String, NSImage)] = []
var i = 3
while i + 1 < args.count { items.append((args[i], NSImage(contentsOfFile: args[i+1])!)); i += 2 }
let cellW: CGFloat = 800, cellH: CGFloat = 533, pad: CGFloat = 10, head: CGFloat = 50, lab: CGFloat = 36
let cols = 2, rows = (items.count + 1) / 2
let W = CGFloat(cols) * cellW + CGFloat(cols + 1) * pad
let Hh = head + CGFloat(rows) * (cellH + lab) + CGFloat(rows + 1) * pad
let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(W), pixelsHigh: Int(Hh), bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
NSColor(white: 0.12, alpha: 1).setFill(); NSRect(x: 0, y: 0, width: W, height: Hh).fill()
let tAttr: [NSAttributedString.Key: Any] = [.font: NSFont.boldSystemFont(ofSize: 26), .foregroundColor: NSColor.white]
(title as NSString).draw(at: NSPoint(x: pad, y: Hh - head + 10), withAttributes: tAttr)
let lAttr: [NSAttributedString.Key: Any] = [.font: NSFont.boldSystemFont(ofSize: 22), .foregroundColor: NSColor.white]
for (k, item) in items.enumerated() {
  let c = k % cols, r = k / cols
  let x = pad + CGFloat(c) * (cellW + pad)
  let yTop = Hh - head - pad - CGFloat(r) * (cellH + lab + pad)
  (item.0 as NSString).draw(at: NSPoint(x: x, y: yTop - lab + 6), withAttributes: lAttr)
  item.1.draw(in: NSRect(x: x, y: yTop - lab - cellH, width: cellW, height: cellH))
}
NSGraphicsContext.restoreGraphicsState()
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: out))
