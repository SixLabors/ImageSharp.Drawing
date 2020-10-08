from sympy import Point, Polygon, Line
from sympy.geometry.point import Point2D
from sympy.geometry.line import Segment2D

def HLine(y):
    s = Point(-1000,y)
    e = Point(1000,y)
    return Line(s,e)

def Points2Str(pts):
    res = "";

    for i in range(len(pts)):
        v = pts[i]
        res += "("+str(v.x.evalf(8))+","+str(v.y.evalf(8))+")"
        if i != len(pts)-1:
            res += ", "

    return res

def Isec2Str(pts):
    
    coords = []
    for v in pts:
        if type(v) is Point2D:
            coords.append(v.x.evalf(8))
        if type(v) is Segment2D:
            coords.append(v.p1.x.evalf(8))
            coords.append(v.p2.x.evalf(8))
        #else:
        #    print type(v)
    coords.sort()

    res = "";
    for i in range(len(coords)):
        res += str(coords[i])+"f"
        if i != len(coords)-1:
            res += ", "
    
    return res

def Poly2Str(poly):
    return PrintPoints(poly.vertices)

def Scan(poly, min, max, step):
    y = min
    while y <= max:
        line = HLine(y)
        isc = poly.intersection(line)
        #print str(y) + " >> " + Points2Str(isc)
        print "new float[] { " + Isec2Str(isc) + " },"
        y+=step

poly1 = Polygon( (1, 3), (1, 2), (5, 2), (5, 5), (2, 5), (2, 1), (3, 1), (3, 4), (4, 4), (4, 3), (1, 3) )

Scan(poly1,1,5,0.5)
