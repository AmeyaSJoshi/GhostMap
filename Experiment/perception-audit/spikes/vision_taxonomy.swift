// RESEARCH SPIKE — NOT PRODUCTION. Lists Apple Vision's built-in image-classification taxonomy.
import Vision
let req = VNClassifyImageRequest()
let ids = (try? req.supportedIdentifiers()) ?? []
print("total:", ids.count, "revision:", req.revision)
let keys = ["chair","sofa","couch","bed","desk","table","dresser","wardrobe","cabinet","shelf","bookcase","lamp","television","monitor","computer","refrigerator","sink","toilet","plant","door","window","curtain","blind","rug","carpet","mirror","stool","bench","ottoman","nightstand","drawer","closet","oven","microwave","washing","box","furniture","piano","fan","radiator","bathtub","shower","stair","fireplace","whiteboard","blackboard","classroom","bedroom","kitchen","office","living","bathroom","interior","room"]
for k in keys { let m = ids.filter { $0.contains(k) }; if !m.isEmpty { print(k, "->", m.joined(separator: ", ")) } }
