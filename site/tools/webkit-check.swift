// The site in WebKit (Safari's engine), the ways real visits go wrong that a fresh, always-visible headless Chrome
// never shows. Run it before calling the site done:
//   cd site && swift tools/webkit-check.swift http://127.0.0.1:48780
// Each check runs in its own process (windows left over from one check can stop WebKit animating the next) with one
// invisible window: fully transparent and ignoring the mouse, so nothing shows on screen (WebKit only animates
// windows that are on screen). Exit code 1 if any check fails.
import AppKit
import WebKit

let args = CommandLine.arguments
let base = args.count > 1 ? args[1] : "http://127.0.0.1:48780"
let only = args.count > 3 && args[2] == "--one" ? Int(args[3]) : nil
let app = NSApplication.shared
app.setActivationPolicy(.prohibited)

struct Check {
  let name: String, path: String, w: CGFloat, h: CGFloat
  var hiddenFor = 0.0          // seconds the window is hidden (off screen) before the person comes back
  var block: String? = nil     // a file that fails to load (content-blocker url-filter)
  let expect: String           // "film" (the 3D is on) or "story" (the still pictures)
  var storyFirst = false       // the story must show first, then the film come back
  let within: Double           // seconds after the window shows
}
let checks = [
  Check(name: "first visit, laptop", path: "/", w: 1440, h: 900, expect: "film", within: 15),
  Check(name: "first visit, phone size", path: "/", w: 390, h: 844, expect: "film", within: 15),
  Check(name: "opened behind another window for 12 s", path: "/", w: 1440, h: 900, hiddenFor: 12, expect: "film", within: 10),
  Check(name: "very slow device: story at 9 s, then the film comes back", path: "/?test&stall=10", w: 1440, h: 900, expect: "film", storyFirst: true, within: 22),
  Check(name: "a page file fails to load: one reload, then the story", path: "/", w: 1440, h: 900, block: "haptics\\\\.js", expect: "story", within: 20),
]
let state = "JSON.stringify({ cls: document.documentElement.className, errs: globalThis.__errs || [] })"
var failed = 0

// The parent: run every check as its own process, one after another.
if only == nil {
  let me = URL(fileURLWithPath: #filePath).path
  for i in checks.indices {
    let p = Process()
    p.executableURL = URL(fileURLWithPath: "/usr/bin/env")
    p.arguments = ["swift", me, base, "--one", String(i)]
    try! p.run(); p.waitUntilExit()
    if p.terminationStatus != 0 { failed += 1 }
  }
  print(failed == 0 ? "ALL PASS" : "\(failed) FAILED")
  exit(failed == 0 ? 0 : 1)
}

func run(_ i: Int) {
  let c = checks[i]
  let cfg = WKWebViewConfiguration()
  cfg.websiteDataStore = .nonPersistent()
  func start() {
    let win = NSWindow(contentRect: NSRect(x: c.hiddenFor > 0 ? -6000 : 0, y: 0, width: c.w, height: c.h), styleMask: [.borderless], backing: .buffered, defer: false)
    win.alphaValue = 0; win.ignoresMouseEvents = true; win.isReleasedWhenClosed = false
    let web = WKWebView(frame: NSRect(x: 0, y: 0, width: c.w, height: c.h), configuration: cfg)
    win.contentView!.addSubview(web)
    win.orderFrontRegardless()
    web.load(URLRequest(url: URL(string: base + c.path)!))
    DispatchQueue.main.asyncAfter(deadline: .now() + c.hiddenFor) {
      win.setFrameOrigin(.zero)
      var sawStory = false, waited = 0.0
      func poll() {
        web.evaluateJavaScript(state) { r, _ in
          let s = (r as? String) ?? "{}"
          let o = (try? JSONSerialization.jsonObject(with: Data(s.utf8))) as? [String: Any] ?? [:]
          let cls = (o["cls"] as? String ?? "").split(separator: " ").map(String.init)
          let film = cls.contains("gl-on") && !cls.contains("no-gl"), story = cls.contains("no-gl") && !cls.contains("gl-on")
          if story { sawStory = true }
          let ok = c.expect == "film" ? film && (!c.storyFirst || sawStory) : story && "\(o["errs"] ?? "")".contains("failed to load")
          if ok || waited >= c.within {
            if !ok { failed += 1 }
            print(ok ? "PASS" : "FAIL", c.name, String(format: "(%.1f s)", waited), ok ? "" : "classes: \(cls.joined(separator: " ")) errors: \(o["errs"] ?? [])")
            exit(ok ? 0 : 1)
          }
          waited += 0.5
          DispatchQueue.main.asyncAfter(deadline: .now() + 0.5, execute: poll)
        }
      }
      poll()
    }
  }
  if let b = c.block {
    let rules = "[{\"trigger\":{\"url-filter\":\"\(b)\"},\"action\":{\"type\":\"block\"}}]"
    WKContentRuleListStore.default().compileContentRuleList(forIdentifier: "webkit-check-\(i)", encodedContentRuleList: rules) { list, err in
      if let list { cfg.userContentController.add(list) } else { print("could not set up the blocked file:", err as Any) }
      start()
    }
  } else { start() }
}
run(only!)
app.run()
