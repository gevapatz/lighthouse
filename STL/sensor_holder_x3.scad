ard_hole_dia = 3.2;
ard_hole_spacing = 27.9;
thickness = 3;
extension = 2;
board_tab_len = 5;
arm_width = 10;
arm_length = 50;
sensor_tilt = 10; // degrees up
sensor_height = 12;
connector_width = 11;
connector_height = 3.2; // really 2.6, but compensates for shrinkage
arc_width = 5;

module 2D_arc(w, r, deg=90,fn = 100 ) {
	render() {
		difference() {
			// outer circle
			circle(r=r+w/2,center=true,$fn=fn);
			// outer circle
			circle(r=r-w/2,center=true,$fn=fn);

		//remove the areas not covered by the arc
		translate([sin(90-deg/2)*((r+w/2)*2),
						-sin(deg/2)*((r+w/2)*2)])
			rotate([0,0,90-deg/2])
				translate([((r+w/2))-sin(90-(deg)/4)*((r+w/2)),
							((r+w/2))*2-sin(90-(deg)/4)*((r+w/2))])
					square([sin(90-deg/4)*((r+w/2)*2),
								sin(90-deg/4)*((r+w/2)*2)],center=true);
		translate([-sin(90-(deg)/2)*((r+w/2)*2),
						-sin(deg/2)*((r+w/2)*2)])
			rotate([0,0,-(90-deg/2)])
			  translate([-((r+w/2))+sin(90-(deg)/4)*((r+w/2)),
							((r+w/2))*2-sin(90-(deg)/4)*((r+w/2))])
					square([sin(90-deg/4)*((r+w/2)*2),
								sin(90-deg/4)*((r+w/2)*2)],center=true);
		}
	}
}

module 3D_arc(w, r, deg, h,fn) {
	linear_extrude(h)
			2D_arc(w, r, deg,fn);

}


module sensor_arm() {   
    cube([arm_length, arm_width, thickness]);   
    translate([arm_length-thickness/2, 0, 0])
    rotate([90-sensor_tilt,0,90]) {
        difference() {
            translate([-extension,0,0])
            cube([connector_width + extension*2, sensor_height+thickness, thickness]);
            translate([0,sensor_height+thickness-connector_height-extension, -0.1])
                cube([connector_width, connector_height, thickness+0.2]);
        }
    }
}

difference() {
    union(){
        translate([(ard_hole_spacing+ard_hole_dia)/2 + extension,0,0])
        rotate([0,0,180])
            3D_arc(arc_width,35, 100, thickness, 100);
        cube([ard_hole_spacing + ard_hole_dia + (extension *2), 
            ard_hole_dia + (extension * 2), 
            thickness]);
        translate([0,5,0])
            rotate([0,0,270-30])
                sensor_arm();
        translate([ard_hole_spacing - ard_hole_dia+extension,0,0])
            rotate([0,0,270+30])
                sensor_arm();
        translate([(ard_hole_spacing+ard_hole_dia)/2 + extension-arm_width/2,0,0])
            rotate([0,0,270])
                sensor_arm();
        translate([ard_hole_dia + extension,
                    ard_hole_dia + (extension * 2),0])
            cube([ard_hole_spacing - ard_hole_dia,
                  board_tab_len, thickness]);
    }
    translate([ard_hole_dia/2+extension,
                ard_hole_dia/2+extension,
                -.1])
        cylinder(r=ard_hole_dia/2, h=thickness+0.2, $fn=100);
    translate([ard_hole_dia/2+extension+ard_hole_spacing,
                ard_hole_dia/2+extension,
                -.1])
        cylinder(r=ard_hole_dia/2, h=thickness+0.2, $fn=100);
}